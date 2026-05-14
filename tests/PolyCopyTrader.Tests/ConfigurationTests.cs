using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;

namespace PolyCopyTrader.Tests;

public sealed class ConfigurationTests
{
    [Fact]
    public void DefaultConfiguration_IsValid()
    {
        var configuration = new AppConfiguration();

        var errors = AppOptionsValidator.Validate(configuration);

        Assert.Empty(errors);
        Assert.Equal(MarketDataWebSocketSubscriptionScope.AllActiveMarkets, configuration.MarketDataWebSocket.SubscriptionScope);
        Assert.Equal(0, configuration.MarketDataWebSocket.MaxSubscribedAssets);
        Assert.Equal(1_000, configuration.MarketDataWebSocket.SubscriptionBatchSize);
        Assert.Equal(3_000, configuration.MarketDataWebSocket.ShardMaxAssets);
        Assert.Equal(64, configuration.MarketDataWebSocket.MaxShardConnections);
        Assert.Equal(10, configuration.MarketDataWebSocket.WatchdogIntervalSeconds);
        Assert.Equal(90, configuration.MarketDataWebSocket.WatchdogStaleSeconds);
        Assert.False(configuration.MarketDataWebSocket.PersistOrderBookSnapshots);
        Assert.False(configuration.MarketDataWebSocket.PersistMarketDataEvents);
        Assert.Equal(60, configuration.MarketDataWebSocket.StatusPersistIntervalSeconds);
        Assert.Equal(1000, configuration.Polymarket.RetryBaseDelayMilliseconds);
        Assert.True(configuration.PolymarketHttpLogging.Enabled);
        Assert.False(configuration.PolymarketHttpLogging.PersistSuccessfulRequests);
        Assert.Equal(0, configuration.PolymarketHttpLogging.SuccessfulRequestSampleRate);
        Assert.True(configuration.PolymarketHttpLogging.PersistNetworkErrors);
        Assert.True(configuration.PolymarketHttpLogging.PersistRateLimitedRequests);
        Assert.True(configuration.PolymarketHttpLogging.PersistAuthFailures);
        Assert.True(configuration.PolymarketHttpLogging.PersistServerErrors);
        Assert.False(configuration.PolymarketHttpLogging.PersistOtherClientErrors);
        Assert.False(configuration.PolymarketHttpLogging.PersistNotFound);
        Assert.True(configuration.PolymarketHttpLogging.CleanupEnabled);
        Assert.Equal(10, configuration.PolymarketHttpLogging.CleanupIntervalMinutes);
        Assert.Equal(25_000, configuration.PolymarketHttpLogging.CleanupBatchSize);
        Assert.Equal(2, configuration.PolymarketHttpLogging.CleanupMaxBatchesPerCycle);
        Assert.Equal(6, configuration.PolymarketHttpLogging.SuccessfulRetentionHours);
        Assert.Equal(14, configuration.PolymarketHttpLogging.FailedRetentionDays);
        Assert.Equal(0, configuration.GammaMarketIngestion.PollIntervalSeconds);
        Assert.Equal(500, configuration.GammaMarketIngestion.PageLimit);
        Assert.Equal(1.00m, configuration.BtcUpDown5mStrategy.StakeUsd);
        Assert.Equal(1, configuration.BtcUpDown5mStrategy.MartinStakeLevels);
        Assert.False(configuration.BtcUpDown5mStrategy.PaperTakerPricingEnabled);
        Assert.True(configuration.BtcUpDown5mStrategy.PaperTakerRestFallbackEnabled);
        Assert.Equal(1_500, configuration.BtcUpDown5mStrategy.PaperTakerMaxQuoteAgeMilliseconds);
        Assert.Equal(0.80m, configuration.BtcUpDown5mStrategy.PaperTakerMaxEntryPrice);
        Assert.Equal(0.03m, configuration.BtcUpDown5mStrategy.PaperTakerMaxReferenceSlippage);
        Assert.Equal(0.10m, configuration.BtcUpDown5mStrategy.PaperTakerMaxSpreadAbs);
        Assert.Equal(0.15m, configuration.BtcUpDown5mStrategy.PaperTakerMaxGammaClobDiff);
        Assert.Equal(16, configuration.BtcUpDown5mStrategy.MaxConcurrentSettlements);
        Assert.True(configuration.BtcUpDown5mStrategy.OpeningLimitDynamicBreakEvenPricingEnabled);
        Assert.Equal(100, configuration.BtcUpDown5mStrategy.OpeningLimitBreakEvenLookbackRuns);
        Assert.Equal(30, configuration.BtcUpDown5mStrategy.OpeningLimitBreakEvenMinSettledRuns);
        Assert.Equal(0.10m, configuration.BtcUpDown5mStrategy.OpeningLimitBreakEvenMargin);
        Assert.Equal(0.50m, configuration.BtcUpDown5mStrategy.OpeningLimitMaxPrice);
        Assert.Equal(0.01m, configuration.BtcUpDown5mStrategy.OpeningLimitPriceTickSize);
        Assert.Equal(120, configuration.BtcUpDown5mStrategy.OpeningLimitGtdTtlSeconds);
        Assert.Equal(60, configuration.BtcUpDown5mStrategy.OpeningLimitExpireBeforeMarketEndSeconds);
        Assert.Equal(60, configuration.BtcUpDown5mStrategy.ClobGtdExpirationSecurityBufferSeconds);
        Assert.Equal(0.0001m, configuration.BtcUpDown5mStrategy.PreviousScoreCounterTrendEpsilonScore);
        Assert.Equal(10, configuration.BtcUpDown5mStrategy.PreviousScoreCounterTrendMinSamples);
        Assert.Equal(0.10m, configuration.BtcUpDown5mStrategy.PreviousScoreCounterTrendWinsorPercent);
        Assert.False(configuration.BtcUpDown5mStrategy.PreviousScoreCounterTrendEnableTimeShareFilter);
        Assert.Equal(0.50m, configuration.BtcUpDown5mStrategy.PreviousScoreCounterTrendMinUpTimeShare);
        Assert.Equal(0.50m, configuration.BtcUpDown5mStrategy.PreviousScoreCounterTrendMinDownTimeShare);
        Assert.False(configuration.CoinbaseExchange.Enabled);
        Assert.Equal("https://api.exchange.coinbase.com", configuration.CoinbaseExchange.BaseUrl);
        Assert.Equal("BTC-USD", configuration.CoinbaseExchange.ProductId);
        Assert.Equal(60, configuration.CoinbaseExchange.PollIntervalSeconds);
        Assert.Equal(100, configuration.CoinbaseExchange.WindowSize);
        Assert.Equal(15, configuration.CoinbaseExchange.TimeoutSeconds);
        Assert.Equal("PolyCopyTrader/1.0 BTC-USD-reference", configuration.CoinbaseExchange.UserAgent);
        Assert.True(configuration.BinanceBtcUsdReference.Enabled);
        Assert.Equal("wss://data-stream.binance.vision:443/ws/btcusdt@trade", configuration.BinanceBtcUsdReference.StreamUrl);
        Assert.Equal(60, configuration.BinanceBtcUsdReference.SampleIntervalSeconds);
        Assert.Equal(100, configuration.BinanceBtcUsdReference.WindowSize);
        Assert.Equal(5, configuration.BinanceBtcUsdReference.StaleAfterSeconds);
        Assert.True(configuration.BinanceCryptoReference.Enabled);
        Assert.Equal("wss://data-stream.binance.vision:443/stream", configuration.BinanceCryptoReference.CombinedStreamBaseUrl);
        Assert.Equal(["ETH", "SOL", "XRP"], configuration.BinanceCryptoReference.AssetSymbols);
        Assert.Equal(5, configuration.BinanceCryptoReference.StaleAfterSeconds);
        Assert.True(configuration.BtcUpDown5mOddsArchive.Enabled);
        Assert.Equal(5, configuration.BtcUpDown5mOddsArchive.PollIntervalSeconds);
        Assert.Equal(500, configuration.BtcUpDown5mOddsArchive.MaxMarketsPerCycle);
        Assert.Equal(15_000, configuration.BtcUpDown5mOddsArchive.MaxOrderBookAgeMilliseconds);
        Assert.True(configuration.BtcUpDown5mOddsArchive.RestFallbackEnabled);
        Assert.True(configuration.BtcUpDown5mStatistics.Enabled);
        Assert.Equal(1, configuration.BtcUpDown5mStatistics.PollIntervalSeconds);
        Assert.Equal(500, configuration.BtcUpDown5mStatistics.MaxMarketsPerCycle);
        Assert.Equal(20, configuration.BtcUpDown5mStatistics.MinHistorySupport);
        Assert.Equal(0m, configuration.BtcUpDown5mStatistics.MinimumEdge);
        Assert.Equal(5, configuration.BtcUpDown5mStatistics.HistorySecondsStep);
        Assert.Equal(5, configuration.BtcUpDown5mStatistics.HistoryCentsStep);
        Assert.Equal(295, configuration.BtcUpDown5mStatistics.HistoryMaxSeconds);
        Assert.Equal(2, configuration.BtcUpDown5mStatistics.HistorySampleOffsetSeconds);
        Assert.Equal(15_000, configuration.BtcUpDown5mStatistics.MaxOrderBookAgeMilliseconds);
        Assert.True(configuration.BtcUpDown5mStatistics.RestFallbackEnabled);
        Assert.Equal(30, configuration.BtcUpDown5mStatistics.ResultSettlementDelaySeconds);
        Assert.Equal(60, configuration.BtcUpDown5mStatistics.ResultRetryDelaySeconds);
        Assert.Equal(500, configuration.BtcUpDown5mStatistics.MaxHistorySettlementsPerCycle);
        Assert.True(configuration.CryptoUpDown5mOddsArchive.Enabled);
        Assert.Equal(["ETH", "SOL", "XRP"], configuration.CryptoUpDown5mOddsArchive.AssetSymbols);
        Assert.Equal(5, configuration.CryptoUpDown5mOddsArchive.PollIntervalSeconds);
        Assert.Equal(500, configuration.CryptoUpDown5mOddsArchive.MaxMarketsPerCycle);
        Assert.Equal(15_000, configuration.CryptoUpDown5mOddsArchive.MaxOrderBookAgeMilliseconds);
        Assert.True(configuration.CryptoUpDown5mOddsArchive.RestFallbackEnabled);
        Assert.True(configuration.ChainlinkBtcUsdDiagnostics.Enabled);
        Assert.Equal("https://data.chain.link", configuration.ChainlinkBtcUsdDiagnostics.BaseUrl);
        Assert.Equal("0x00039d9e45394f473ab1f050a1b963e6b05351e52d71e507509ada0c95ed75b8", configuration.ChainlinkBtcUsdDiagnostics.FeedId);
        Assert.Equal(10, configuration.ChainlinkBtcUsdDiagnostics.PollIntervalSeconds);
        Assert.Equal(15, configuration.ChainlinkBtcUsdDiagnostics.TimeoutSeconds);
        Assert.Equal(30, configuration.ChainlinkBtcUsdDiagnostics.MaxNearestAgeSeconds);
        Assert.Equal("1m", configuration.ChainlinkBtcUsdDiagnostics.QueryWindow);
        Assert.True(configuration.OnChainIngestion.TradeCaptureEnabled);
        Assert.Equal(250, configuration.OnChainIngestion.TradeCapturePollDelayMilliseconds);
        Assert.Equal(0, configuration.OnChainIngestion.TradeCaptureRequestDelayMilliseconds);
        Assert.Equal(20, configuration.OnChainIngestion.TradeCaptureStartLookbackBlocks);
        Assert.True(configuration.OnChainIngestion.TradeCapturePersistCaptures);
        Assert.False(configuration.OnChainIngestion.TradeCaptureSkipStaleCursor);
        Assert.Equal(2, configuration.OnChainIngestion.TradeCaptureMaxCursorLagBlocks);
        Assert.Equal(0, configuration.OnChainIngestion.TradeCaptureConfirmations);
        Assert.Equal(1_000, configuration.OnChainIngestion.TradeCaptureErrorDelayMilliseconds);
        Assert.Equal(30_000, configuration.OnChainIngestion.TradeCaptureMaxErrorDelayMilliseconds);
        Assert.False(configuration.OnChainIngestion.PaperSignalEnabled);
        Assert.True(configuration.OnChainIngestion.PaperSignalBacklogEnabled);
        Assert.True(configuration.OnChainIngestion.PaperSignalHotPathEnabled);
        Assert.Equal(2, configuration.OnChainIngestion.PaperSignalHotMaxAgeSeconds);
        Assert.Equal(100, configuration.OnChainIngestion.PaperSignalLatestCandidatesLimit);
        Assert.Equal(250, configuration.OnChainIngestion.PaperSignalPollDelayMilliseconds);
        Assert.Equal(250, configuration.OnChainIngestion.PaperSignalBatchSize);
        Assert.Equal(300, configuration.OnChainIngestion.PaperSignalMaxLagSeconds);
        Assert.Equal(24, configuration.OnChainIngestion.PaperSignalRatingStaleAfterHours);
        Assert.False(configuration.MarketTradeDiagnostics.Enabled);
        Assert.Equal(1_000, configuration.MarketTradeDiagnostics.MarketTradesLimit);
        Assert.Equal(5, configuration.MarketTradeDiagnostics.MatchTimestampToleranceSeconds);
        Assert.True(configuration.DataApiTraderIngestion.Enabled);
        Assert.Equal(1_000, configuration.DataApiTraderIngestion.GlobalTradesLimit);
        Assert.Equal(0, configuration.DataApiTraderIngestion.PollDelayMilliseconds);
        Assert.Equal(1_000, configuration.DataApiTraderIngestion.UserTradesLimit);
        Assert.Equal(3_000, configuration.DataApiTraderIngestion.MaxUserHistoricalOffset);
        Assert.Equal(5, configuration.DataApiTraderIngestion.SyncBatchSize);
        Assert.Equal(1_000, configuration.DataApiTraderIngestion.SyncPollDelayMilliseconds);
        Assert.Equal(3_600, configuration.DataApiTraderIngestion.ExistingTraderRefreshIntervalSeconds);
        Assert.False(configuration.DataApiTraderIngestion.RefreshPositionsEnabled);
        Assert.True(configuration.DataApiTraderIngestion.RefreshPolymarketRatingsEnabled);
        Assert.Equal("ALL", configuration.DataApiTraderIngestion.PolymarketRatingTimePeriod);
        Assert.Equal("PNL", configuration.DataApiTraderIngestion.PolymarketRatingOrderBy);
        Assert.Equal(3_600, configuration.DataApiTraderIngestion.PolymarketRatingRefreshIntervalSeconds);
        Assert.Equal(60, configuration.DataApiTraderIngestion.PolymarketRatingFailureDelaySeconds);
        Assert.Equal(0, configuration.DataApiTraderIngestion.PolymarketRatingRequestDelayMilliseconds);
        Assert.True(configuration.DataApiTraderIngestion.PolymarketRatingPositionsEnabled);
        Assert.Equal(500, configuration.DataApiTraderIngestion.PolymarketRatingCurrentPositionsLimit);
        Assert.Equal(0, configuration.DataApiTraderIngestion.PolymarketRatingMaxCurrentPositionsOffset);
        Assert.Equal(50, configuration.DataApiTraderIngestion.PolymarketRatingClosedPositionsLimit);
        Assert.Equal(0, configuration.DataApiTraderIngestion.PolymarketRatingMaxClosedPositionsOffset);
        Assert.Equal(1_000, configuration.DataApiTraderIngestion.MaxPositionRefreshesPerCycle);
        Assert.Equal(500, configuration.DataApiTraderIngestion.CurrentPositionsLimit);
        Assert.Equal(10_000, configuration.DataApiTraderIngestion.MaxCurrentPositionsOffset);
        Assert.Equal(50, configuration.DataApiTraderIngestion.ClosedPositionsLimit);
        Assert.Equal(100_000, configuration.DataApiTraderIngestion.MaxClosedPositionsOffset);
        Assert.Equal(0.10m, configuration.Execution.MinLeaderTradeUsd);
        Assert.True(configuration.Signal.CopiedTraderPerformanceGuardEnabled);
        Assert.Equal(3, configuration.Signal.CopiedTraderPerformanceMinSettledPositions);
        Assert.Equal(-2m, configuration.Signal.CopiedTraderPerformanceMinTotalPnlUsd);
        Assert.Equal(-10m, configuration.Signal.CopiedTraderPerformanceMinRoiPct);
        Assert.Equal(35m, configuration.Signal.CopiedTraderPerformanceMinScore);
        Assert.Equal(10, configuration.Signal.CopiedTraderPerformanceScore);
        Assert.False(configuration.PaperTrading.RunInLiveMode);
        Assert.Equal(5, configuration.PaperTrading.OpenOrderProcessingIntervalSeconds);
        Assert.Equal(100, configuration.PaperTrading.OpenOrderFillSimulationBatchSize);
        Assert.True(configuration.PaperTrading.LeaderActivityExitTrackingEnabled);
        Assert.Equal(1_000, configuration.PaperTrading.LeaderActivityExitTrackingPollDelayMilliseconds);
        Assert.Equal(100, configuration.PaperTrading.LeaderActivityExitTrackingBatchSize);
        Assert.Equal(500, configuration.PaperTrading.LeaderActivityExitTrackingActivityLimit);
        Assert.Equal(0, configuration.PaperTrading.LeaderActivityExitTrackingRequestDelayMilliseconds);
        Assert.Equal(1_000, configuration.PaperTrading.LeaderActivityExitTrackingErrorDelayMilliseconds);
        Assert.Equal(30_000, configuration.PaperTrading.LeaderActivityExitTrackingMaxErrorDelayMilliseconds);
        Assert.Equal(5, configuration.LiveTrading.MaintenancePollIntervalSeconds);
        Assert.Equal(60, configuration.Dashboard.RefreshIntervalSeconds);
        Assert.Equal(60, configuration.Dashboard.StrategyRefreshIntervalSeconds);
    }

    [Fact]
    public void InvalidConfiguration_ReturnsClearErrors()
    {
        var configuration = new AppConfiguration
        {
            Bot = new BotOptions
            {
                PollIntervalSeconds = 0
            },
            Polymarket = new PolymarketOptions
            {
                DataApiBaseUrl = "not-a-url",
                ClobBaseUrl = "https://clob.polymarket.com",
                GammaBaseUrl = "https://gamma-api.polymarket.com",
                GeoblockUrl = "https://polymarket.com/api/geoblock"
            },
            MarketDataWebSocket = new MarketDataWebSocketOptions
            {
                SubscriptionScope = (MarketDataWebSocketSubscriptionScope)999,
                SubscriptionBatchSize = 0,
                ShardMaxAssets = 0,
                MaxShardConnections = -1,
                WatchdogIntervalSeconds = 0,
                WatchdogStaleSeconds = 1,
                StatusPersistIntervalSeconds = 0
            },
            Dashboard = new DashboardOptions
            {
                RefreshIntervalSeconds = 0,
                StrategyRefreshIntervalSeconds = 0
            },
            PolymarketHttpLogging = new PolymarketHttpLoggingOptions
            {
                SuccessfulRequestSampleRate = -1,
                CleanupIntervalMinutes = 0,
                CleanupBatchSize = 0,
                CleanupMaxBatchesPerCycle = 0,
                SuccessfulRetentionHours = 0,
                FailedRetentionDays = 0
            },
            LiveTrading = new LiveTradingOptions
            {
                MaintenancePollIntervalSeconds = 0
            }
        };

        var errors = AppOptionsValidator.Validate(configuration);

        Assert.Contains(errors, error => error.Contains("PollIntervalSeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("DataApiBaseUrl", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("MarketDataWebSocket.SubscriptionScope", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("MarketDataWebSocket.SubscriptionBatchSize", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("MarketDataWebSocket.ShardMaxAssets", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("MarketDataWebSocket.MaxShardConnections", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("MarketDataWebSocket.WatchdogIntervalSeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("MarketDataWebSocket.WatchdogStaleSeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("MarketDataWebSocket.StatusPersistIntervalSeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Dashboard.RefreshIntervalSeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Dashboard.StrategyRefreshIntervalSeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("PolymarketHttpLogging.SuccessfulRequestSampleRate", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("PolymarketHttpLogging.CleanupIntervalMinutes", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("PolymarketHttpLogging.CleanupBatchSize", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("PolymarketHttpLogging.CleanupMaxBatchesPerCycle", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("PolymarketHttpLogging.SuccessfulRetentionHours", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("PolymarketHttpLogging.FailedRetentionDays", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("LiveTrading.MaintenancePollIntervalSeconds", StringComparison.Ordinal));
    }

    [Fact]
    public void GammaMarketIngestionOptions_AreValidated()
    {
        var configuration = new AppConfiguration
        {
            GammaMarketIngestion = new GammaMarketIngestionOptions
            {
                PollIntervalSeconds = -1,
                PageLimit = 0
            }
        };

        var errors = AppOptionsValidator.Validate(configuration);

        Assert.Contains(errors, error => error.Contains("GammaMarketIngestion.PollIntervalSeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("GammaMarketIngestion.PageLimit", StringComparison.Ordinal));
    }

    [Fact]
    public void BtcUpDown5mStrategyOptions_AreValidated()
    {
        var configuration = new AppConfiguration
        {
            BtcUpDown5mStrategy = new BtcUpDown5mStrategyOptions
            {
                PaperTakerMaxQuoteAgeMilliseconds = 0,
                PaperTakerMaxEntryPrice = 0m,
                PaperTakerMaxReferenceSlippage = -0.01m,
                PaperTakerMaxSpreadAbs = -0.01m,
                PaperTakerMaxGammaClobDiff = -0.01m,
                OpeningLimitBreakEvenLookbackRuns = 0,
                OpeningLimitBreakEvenMinSettledRuns = 2,
                OpeningLimitBreakEvenMargin = -0.01m,
                OpeningLimitMaxPrice = 0.51m,
                OpeningLimitPriceTickSize = 0m,
                OpeningLimitGtdTtlSeconds = 29,
                OpeningLimitExpireBeforeMarketEndSeconds = -1,
                ClobGtdExpirationSecurityBufferSeconds = 59,
                PreviousScoreCounterTrendEpsilonScore = -0.01m,
                PreviousScoreCounterTrendMinSamples = 1,
                PreviousScoreCounterTrendWinsorPercent = 0.50m,
                PreviousScoreCounterTrendMinUpTimeShare = -0.01m,
                PreviousScoreCounterTrendMinDownTimeShare = 1.01m,
                MaxConcurrentSettlements = 0,
                OrderBookRefreshIntervalMilliseconds = 99,
                OrderBookRefreshMaxMarketsPerCycle = 0,
                OrderBookRefreshMarketLookaheadSeconds = -1,
                OrderBookRefreshMarketBehindSeconds = -1,
                OrderBookRefreshRequestTimeoutSeconds = 0
            }
        };

        var errors = AppOptionsValidator.Validate(configuration);

        Assert.Contains(errors, error => error.Contains("BtcUpDown5mStrategy.PaperTakerMaxQuoteAgeMilliseconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BtcUpDown5mStrategy.PaperTakerMaxEntryPrice", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BtcUpDown5mStrategy.PaperTakerMaxReferenceSlippage", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BtcUpDown5mStrategy.PaperTakerMaxSpreadAbs", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BtcUpDown5mStrategy.PaperTakerMaxGammaClobDiff", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BtcUpDown5mStrategy.OpeningLimitBreakEvenLookbackRuns", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BtcUpDown5mStrategy.OpeningLimitBreakEvenMinSettledRuns", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BtcUpDown5mStrategy.OpeningLimitBreakEvenMargin", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BtcUpDown5mStrategy.OpeningLimitMaxPrice", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BtcUpDown5mStrategy.OpeningLimitPriceTickSize", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BtcUpDown5mStrategy.OpeningLimitGtdTtlSeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BtcUpDown5mStrategy.OpeningLimitExpireBeforeMarketEndSeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BtcUpDown5mStrategy.ClobGtdExpirationSecurityBufferSeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BtcUpDown5mStrategy.PreviousScoreCounterTrendEpsilonScore", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BtcUpDown5mStrategy.PreviousScoreCounterTrendMinSamples", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BtcUpDown5mStrategy.PreviousScoreCounterTrendWinsorPercent", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BtcUpDown5mStrategy.PreviousScoreCounterTrendMinUpTimeShare", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BtcUpDown5mStrategy.PreviousScoreCounterTrendMinDownTimeShare", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BtcUpDown5mStrategy.MaxConcurrentSettlements", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BtcUpDown5mStrategy.OrderBookRefreshIntervalMilliseconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BtcUpDown5mStrategy.OrderBookRefreshMaxMarketsPerCycle", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BtcUpDown5mStrategy.OrderBookRefreshMarketLookaheadSeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BtcUpDown5mStrategy.OrderBookRefreshMarketBehindSeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BtcUpDown5mStrategy.OrderBookRefreshRequestTimeoutSeconds", StringComparison.Ordinal));
    }

    [Fact]
    public void MarketTradeDiagnosticsOptions_AreValidated()
    {
        var configuration = new AppConfiguration
        {
            MarketTradeDiagnostics = new MarketTradeDiagnosticsOptions
            {
                MarketTradesLimit = 0,
                MatchTimestampToleranceSeconds = -1
            }
        };

        var errors = AppOptionsValidator.Validate(configuration);

        Assert.Contains(errors, error => error.Contains("MarketTradeDiagnostics.MarketTradesLimit", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("MarketTradeDiagnostics.MatchTimestampToleranceSeconds", StringComparison.Ordinal));
    }

    [Fact]
    public void CoinbaseExchangeOptions_AreValidated()
    {
        var configuration = new AppConfiguration
        {
            CoinbaseExchange = new CoinbaseExchangeOptions
            {
                BaseUrl = "http://api.exchange.coinbase.com",
                ProductId = "",
                PollIntervalSeconds = 0,
                WindowSize = 0,
                TimeoutSeconds = 0,
                UserAgent = ""
            }
        };

        var errors = AppOptionsValidator.Validate(configuration);

        Assert.Contains(errors, error => error.Contains("CoinbaseExchange.BaseUrl", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("CoinbaseExchange.ProductId", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("CoinbaseExchange.PollIntervalSeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("CoinbaseExchange.WindowSize", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("CoinbaseExchange.TimeoutSeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("CoinbaseExchange.UserAgent", StringComparison.Ordinal));
    }

    [Fact]
    public void BtcUpDown5mOddsArchiveOptions_AreValidated()
    {
        var configuration = new AppConfiguration
        {
            BtcUpDown5mOddsArchive = new BtcUpDown5mOddsArchiveOptions
            {
                PollIntervalSeconds = 0,
                MaxMarketsPerCycle = 0,
                MaxOrderBookAgeMilliseconds = 0
            }
        };

        var errors = AppOptionsValidator.Validate(configuration);

        Assert.Contains(errors, error => error.Contains("BtcUpDown5mOddsArchive.PollIntervalSeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BtcUpDown5mOddsArchive.MaxMarketsPerCycle", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BtcUpDown5mOddsArchive.MaxOrderBookAgeMilliseconds", StringComparison.Ordinal));
    }

    [Fact]
    public void BtcUpDown5mStatisticsOptions_AreValidated()
    {
        var configuration = new AppConfiguration
        {
            BtcUpDown5mStatistics = new BtcUpDown5mStatisticsOptions
            {
                PollIntervalSeconds = 0,
                MaxMarketsPerCycle = 0,
                MinHistorySupport = 0,
                MinimumEdge = -0.01m,
                HistorySecondsStep = 0,
                HistoryCentsStep = 0,
                HistoryMaxSeconds = 0,
                HistorySampleOffsetSeconds = 5,
                MaxOrderBookAgeMilliseconds = 0,
                ResultSettlementDelaySeconds = -1,
                ResultRetryDelaySeconds = 0,
                MaxHistorySettlementsPerCycle = 0
            }
        };

        var errors = AppOptionsValidator.Validate(configuration);

        Assert.Contains(errors, error => error.Contains("BtcUpDown5mStatistics.PollIntervalSeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BtcUpDown5mStatistics.MaxMarketsPerCycle", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BtcUpDown5mStatistics.MinHistorySupport", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BtcUpDown5mStatistics.MinimumEdge", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BtcUpDown5mStatistics.HistorySecondsStep", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BtcUpDown5mStatistics.HistoryCentsStep", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BtcUpDown5mStatistics.HistoryMaxSeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BtcUpDown5mStatistics.HistorySampleOffsetSeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BtcUpDown5mStatistics.MaxOrderBookAgeMilliseconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BtcUpDown5mStatistics.ResultSettlementDelaySeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BtcUpDown5mStatistics.ResultRetryDelaySeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BtcUpDown5mStatistics.MaxHistorySettlementsPerCycle", StringComparison.Ordinal));
    }

    [Fact]
    public void CryptoUpDown5mOddsArchiveOptions_AreValidated()
    {
        var configuration = new AppConfiguration
        {
            CryptoUpDown5mOddsArchive = new CryptoUpDown5mOddsArchiveOptions
            {
                AssetSymbols = [],
                PollIntervalSeconds = 0,
                MaxMarketsPerCycle = 0,
                MaxOrderBookAgeMilliseconds = 0
            }
        };

        var errors = AppOptionsValidator.Validate(configuration);

        Assert.Contains(errors, error => error.Contains("CryptoUpDown5mOddsArchive.AssetSymbols", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("CryptoUpDown5mOddsArchive.PollIntervalSeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("CryptoUpDown5mOddsArchive.MaxMarketsPerCycle", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("CryptoUpDown5mOddsArchive.MaxOrderBookAgeMilliseconds", StringComparison.Ordinal));
    }

    [Fact]
    public void BinanceBtcUsdReferenceOptions_AreValidated()
    {
        var configuration = new AppConfiguration
        {
            BinanceBtcUsdReference = new BinanceBtcUsdReferenceOptions
            {
                StreamUrl = "https://data-stream.binance.vision/ws/btcusdt@trade",
                SampleIntervalSeconds = 0,
                WindowSize = 0,
                StaleAfterSeconds = 0,
                ReconnectBaseDelaySeconds = 0,
                ReconnectMaxDelaySeconds = 0,
                ReceiveBufferBytes = 100
            }
        };

        var errors = AppOptionsValidator.Validate(configuration);

        Assert.Contains(errors, error => error.Contains("BinanceBtcUsdReference.StreamUrl", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BinanceBtcUsdReference.SampleIntervalSeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BinanceBtcUsdReference.WindowSize", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BinanceBtcUsdReference.StaleAfterSeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BinanceBtcUsdReference.ReconnectBaseDelaySeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BinanceBtcUsdReference.ReconnectMaxDelaySeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BinanceBtcUsdReference.ReceiveBufferBytes", StringComparison.Ordinal));
    }

    [Fact]
    public void BinanceCryptoReferenceOptions_AreValidated()
    {
        var configuration = new AppConfiguration
        {
            BinanceCryptoReference = new BinanceCryptoReferenceOptions
            {
                CombinedStreamBaseUrl = "https://data-stream.binance.vision/stream",
                AssetSymbols = ["ETH", "ETH"],
                StaleAfterSeconds = 0,
                ReconnectBaseDelaySeconds = 0,
                ReconnectMaxDelaySeconds = 0,
                ReceiveBufferBytes = 100
            }
        };

        var errors = AppOptionsValidator.Validate(configuration);

        Assert.Contains(errors, error => error.Contains("BinanceCryptoReference.CombinedStreamBaseUrl", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BinanceCryptoReference.AssetSymbols", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BinanceCryptoReference.StaleAfterSeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BinanceCryptoReference.ReconnectBaseDelaySeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BinanceCryptoReference.ReconnectMaxDelaySeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BinanceCryptoReference.ReceiveBufferBytes", StringComparison.Ordinal));
    }

    [Fact]
    public void ChainlinkBtcUsdDiagnosticsOptions_AreValidated()
    {
        var configuration = new AppConfiguration
        {
            ChainlinkBtcUsdDiagnostics = new ChainlinkBtcUsdDiagnosticsOptions
            {
                BaseUrl = "not-a-url",
                FeedId = "",
                PollIntervalSeconds = 0,
                TimeoutSeconds = 0,
                MaxNearestAgeSeconds = 0,
                QueryWindow = ""
            }
        };

        var errors = AppOptionsValidator.Validate(configuration);

        Assert.Contains(errors, error => error.Contains("ChainlinkBtcUsdDiagnostics.BaseUrl", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("ChainlinkBtcUsdDiagnostics.FeedId", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("ChainlinkBtcUsdDiagnostics.PollIntervalSeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("ChainlinkBtcUsdDiagnostics.TimeoutSeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("ChainlinkBtcUsdDiagnostics.MaxNearestAgeSeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("ChainlinkBtcUsdDiagnostics.QueryWindow", StringComparison.Ordinal));
    }

    [Fact]
    public void OnChainPaperSignalOptions_AreValidated()
    {
        var configuration = new AppConfiguration
        {
            OnChainIngestion = new OnChainIngestionOptions
            {
                PaperSignalPollDelayMilliseconds = -1,
                PaperSignalBatchSize = 0,
                PaperSignalMaxLagSeconds = 0,
                PaperSignalHotMaxAgeSeconds = 0,
                PaperSignalLatestCandidatesLimit = 0,
                PaperSignalRatingStaleAfterHours = 0,
                TradeCaptureMaxCursorLagBlocks = -1
            }
        };

        var errors = AppOptionsValidator.Validate(configuration);

        Assert.Contains(errors, error => error.Contains("OnChainIngestion.PaperSignalPollDelayMilliseconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("OnChainIngestion.PaperSignalBatchSize", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("OnChainIngestion.PaperSignalMaxLagSeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("OnChainIngestion.PaperSignalHotMaxAgeSeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("OnChainIngestion.PaperSignalLatestCandidatesLimit", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("OnChainIngestion.PaperSignalRatingStaleAfterHours", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("OnChainIngestion.TradeCaptureMaxCursorLagBlocks", StringComparison.Ordinal));
    }

    [Fact]
    public void SignalOptions_AreValidated()
    {
        var configuration = new AppConfiguration
        {
            Signal = new SignalOptions
            {
                CopiedTraderPerformanceMinSettledPositions = -1,
                CopiedTraderPerformanceMinRoiPct = -101m,
                CopiedTraderPerformanceMinScore = 101m,
                CopiedTraderPerformanceScore = -1
            }
        };

        var errors = AppOptionsValidator.Validate(configuration);

        Assert.Contains(errors, error => error.Contains("Signal.CopiedTraderPerformanceMinSettledPositions", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Signal.CopiedTraderPerformanceMinRoiPct", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Signal.CopiedTraderPerformanceMinScore", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Signal.CopiedTraderPerformanceScore", StringComparison.Ordinal));
    }

    [Fact]
    public void PaperTradingOptions_AreValidated()
    {
        var configuration = new AppConfiguration
        {
            PaperTrading = new PaperTradingOptions
            {
                InitialBankrollUsd = 0m,
                DefaultOrderTtlSeconds = 0,
                OpenOrderProcessingIntervalSeconds = 0,
                OpenOrderFillSimulationBatchSize = 0,
                SettlementPollIntervalSeconds = 0,
                CopiedTraderPerformanceRefreshSeconds = 0,
                LeaderActivityExitTrackingPollDelayMilliseconds = -1,
                LeaderActivityExitTrackingBatchSize = 0,
                LeaderActivityExitTrackingActivityLimit = 501,
                LeaderActivityExitTrackingRequestDelayMilliseconds = -1,
                LeaderActivityExitTrackingErrorDelayMilliseconds = 1_000,
                LeaderActivityExitTrackingMaxErrorDelayMilliseconds = 999
            }
        };

        var errors = AppOptionsValidator.Validate(configuration);

        Assert.Contains(errors, error => error.Contains("PaperTrading.InitialBankrollUsd", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("PaperTrading.DefaultOrderTtlSeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("PaperTrading.OpenOrderProcessingIntervalSeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("PaperTrading.OpenOrderFillSimulationBatchSize", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("PaperTrading.SettlementPollIntervalSeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("PaperTrading.CopiedTraderPerformanceRefreshSeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("PaperTrading.LeaderActivityExitTrackingPollDelayMilliseconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("PaperTrading.LeaderActivityExitTrackingBatchSize", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("PaperTrading.LeaderActivityExitTrackingActivityLimit", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("PaperTrading.LeaderActivityExitTrackingRequestDelayMilliseconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("PaperTrading.LeaderActivityExitTrackingMaxErrorDelayMilliseconds", StringComparison.Ordinal));
    }

    [Fact]
    public void RuntimeModePolicy_AllowsPaperTradingInLiveModeOnlyWhenEnabled()
    {
        var paperOptions = new PaperTradingOptions();

        Assert.True(RuntimeModePolicy.IsPaperTradingEnabled(new BotOptions { Mode = BotMode.Paper }, paperOptions));
        Assert.False(RuntimeModePolicy.IsPaperTradingEnabled(new BotOptions { Mode = BotMode.Live }, paperOptions));
        Assert.True(RuntimeModePolicy.IsPaperTradingEnabled(
            new BotOptions { Mode = BotMode.Live },
            new PaperTradingOptions { RunInLiveMode = true }));
        Assert.False(RuntimeModePolicy.IsPaperTradingEnabled(new BotOptions { Mode = BotMode.DryRun }, paperOptions));
    }

    [Fact]
    public void DataApiTraderIngestionOptions_AreValidated()
    {
        var configuration = new AppConfiguration
        {
            DataApiTraderIngestion = new DataApiTraderIngestionOptions
            {
                GlobalTradesLimit = 1001,
                PollDelayMilliseconds = -1,
                UserTradesLimit = 0,
                MaxUserHistoricalOffset = -1,
                MaxTradersPerCycle = 0,
                SyncBatchSize = 0,
                SyncPollDelayMilliseconds = -1,
                ExistingTraderRefreshIntervalSeconds = -1,
                PolymarketRatingTimePeriod = "",
                PolymarketRatingOrderBy = "",
                PolymarketRatingRefreshIntervalSeconds = 0,
                PolymarketRatingFailureDelaySeconds = 0,
                PolymarketRatingRequestDelayMilliseconds = -1,
                PolymarketRatingCurrentPositionsLimit = 0,
                PolymarketRatingMaxCurrentPositionsOffset = -1,
                PolymarketRatingClosedPositionsLimit = 0,
                PolymarketRatingMaxClosedPositionsOffset = -1,
                MaxPositionRefreshesPerCycle = -1,
                CurrentPositionsLimit = 0,
                MaxCurrentPositionsOffset = -1,
                ClosedPositionsLimit = 0,
                MaxClosedPositionsOffset = -1,
                ErrorDelayMilliseconds = 0,
                MaxErrorDelayMilliseconds = 1
            }
        };

        var errors = AppOptionsValidator.Validate(configuration);

        Assert.Contains(errors, error => error.Contains("DataApiTraderIngestion.GlobalTradesLimit", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("DataApiTraderIngestion.PollDelayMilliseconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("DataApiTraderIngestion.UserTradesLimit", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("DataApiTraderIngestion.MaxUserHistoricalOffset", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("DataApiTraderIngestion.MaxTradersPerCycle", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("DataApiTraderIngestion.SyncBatchSize", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("DataApiTraderIngestion.SyncPollDelayMilliseconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("DataApiTraderIngestion.ExistingTraderRefreshIntervalSeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("DataApiTraderIngestion.PolymarketRatingTimePeriod", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("DataApiTraderIngestion.PolymarketRatingOrderBy", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("DataApiTraderIngestion.PolymarketRatingRefreshIntervalSeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("DataApiTraderIngestion.PolymarketRatingFailureDelaySeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("DataApiTraderIngestion.PolymarketRatingRequestDelayMilliseconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("DataApiTraderIngestion.PolymarketRatingCurrentPositionsLimit", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("DataApiTraderIngestion.PolymarketRatingMaxCurrentPositionsOffset", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("DataApiTraderIngestion.PolymarketRatingClosedPositionsLimit", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("DataApiTraderIngestion.PolymarketRatingMaxClosedPositionsOffset", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("DataApiTraderIngestion.MaxPositionRefreshesPerCycle", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("DataApiTraderIngestion.CurrentPositionsLimit", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("DataApiTraderIngestion.MaxCurrentPositionsOffset", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("DataApiTraderIngestion.ClosedPositionsLimit", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("DataApiTraderIngestion.MaxClosedPositionsOffset", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("DataApiTraderIngestion.ErrorDelayMilliseconds", StringComparison.Ordinal));
    }

    [Fact]
    public void AuthEnabled_RequiresSigningAddress()
    {
        var configuration = new AppConfiguration
        {
            PolymarketAuth = new PolymarketAuthOptions
            {
                Enabled = true,
                SigningAddress = "not-an-address"
            }
        };

        var errors = AppOptionsValidator.Validate(configuration);

        Assert.Contains(errors, error => error.Contains("SigningAddress", StringComparison.Ordinal));
    }

    [Fact]
    public void LiveTrading_RequiresManualCodeAndAuth()
    {
        var configuration = new AppConfiguration
        {
            Bot = new BotOptions
            {
                Mode = BotMode.Live,
                EnableLiveTrading = true
            }
        };

        var errors = AppOptionsValidator.Validate(configuration);

        Assert.Contains(errors, error => error.Contains("ManualEnableCode", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("PolymarketAuth.Enabled", StringComparison.Ordinal));
    }

    [Fact]
    public void LiveTrading_ConfiguredGateCanValidate()
    {
        var configuration = new AppConfiguration
        {
            Bot = new BotOptions
            {
                Mode = BotMode.Live,
                EnableLiveTrading = true
            },
            LiveTrading = new LiveTradingOptions
            {
                ManualEnableCode = "LIVE_TRADING_ENABLED"
            },
            PolymarketAuth = new PolymarketAuthOptions
            {
                Enabled = true,
                SigningAddress = "0x1111111111111111111111111111111111111111",
                FunderAddress = "0x1111111111111111111111111111111111111111"
            }
        };

        var errors = AppOptionsValidator.Validate(configuration);

        Assert.Empty(errors);
    }

    [Fact]
    public void CertificatePins_RequireValidSha256SpkiFormat()
    {
        var configuration = new AppConfiguration
        {
            Polymarket = new PolymarketOptions
            {
                CertificatePins = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["data-api.polymarket.com"] = ["not-a-pin"]
                }
            }
        };

        var errors = AppOptionsValidator.Validate(configuration);

        Assert.Contains(errors, error => error.Contains("CertificatePins", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("sha256/<base64-spki-hash>", StringComparison.Ordinal));
    }

    [Fact]
    public void CertificatePins_MustMatchConfiguredEndpointHosts()
    {
        var configuration = new AppConfiguration
        {
            Polymarket = new PolymarketOptions
            {
                CertificatePins = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["example.com"] = [ValidCertificatePin()]
                }
            }
        };

        var errors = AppOptionsValidator.Validate(configuration);

        Assert.Contains(errors, error => error.Contains("example.com", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("configured Polymarket endpoint host", StringComparison.Ordinal));
    }

    [Fact]
    public void CertificatePins_AcceptConfiguredEndpointHost()
    {
        var configuration = new AppConfiguration
        {
            Polymarket = new PolymarketOptions
            {
                CertificatePins = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["data-api.polymarket.com"] = [ValidCertificatePin()]
                }
            }
        };

        var errors = AppOptionsValidator.Validate(configuration);

        Assert.Empty(errors);
    }

    [Fact]
    public void SanitizedSummary_DoesNotExposeSecrets()
    {
        var configuration = new AppConfiguration();

        var summary = AppOptionsValidator.ToSanitizedSummary(configuration);

        Assert.Contains("Mode:", summary);
        Assert.Contains("Paper runs in live mode:", summary);
        Assert.DoesNotContain("private", summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", summary, StringComparison.OrdinalIgnoreCase);
    }

    private static string ValidCertificatePin()
    {
        return "sha256/" + Convert.ToBase64String(new byte[32]);
    }
}
