using Microsoft.Extensions.Configuration;
using PolyCopyTrader.Domain.Configuration;

namespace PolyCopyTrader.Service.Configuration;

public static class AppConfigurationLoader
{
    public static AppConfiguration Load(IConfiguration configuration)
    {
        var watchlist = configuration.GetSection("Watchlist").Get<WatchlistOptions>();
        if (watchlist is null)
        {
            var legacyWatchlist = configuration.GetSection("Watchlist").Get<List<TraderRuleOptions>>() ?? [];
            watchlist = new WatchlistOptions { Traders = legacyWatchlist };
        }

        var binanceCryptoReference = NormalizeBinanceCryptoReferenceOptions(
            configuration.GetSection("BinanceCryptoReference").Get<BinanceCryptoReferenceOptions>() ?? new BinanceCryptoReferenceOptions());
        var cryptoUpDown5mOddsArchive = NormalizeCryptoUpDown5mOddsArchiveOptions(
            configuration.GetSection("CryptoUpDown5mOddsArchive").Get<CryptoUpDown5mOddsArchiveOptions>() ?? new CryptoUpDown5mOddsArchiveOptions());

        return new AppConfiguration
        {
            Bot = configuration.GetSection("Bot").Get<BotOptions>() ?? new BotOptions(),
            Risk = configuration.GetSection("Risk").Get<RiskOptions>() ?? new RiskOptions(),
            Execution = configuration.GetSection("Execution").Get<ExecutionOptions>() ?? new ExecutionOptions(),
            Signal = configuration.GetSection("Signal").Get<SignalOptions>() ?? new SignalOptions(),
            Polymarket = configuration.GetSection("Polymarket").Get<PolymarketOptions>() ?? new PolymarketOptions(),
            PolymarketHttpLogging = configuration.GetSection("PolymarketHttpLogging").Get<PolymarketHttpLoggingOptions>() ?? new PolymarketHttpLoggingOptions(),
            PolymarketAuth = configuration.GetSection("PolymarketAuth").Get<PolymarketAuthOptions>() ?? new PolymarketAuthOptions(),
            MarketDataWebSocket = configuration.GetSection("MarketDataWebSocket").Get<MarketDataWebSocketOptions>() ?? new MarketDataWebSocketOptions(),
            MarketTradeDiagnostics = configuration.GetSection("MarketTradeDiagnostics").Get<MarketTradeDiagnosticsOptions>() ?? new MarketTradeDiagnosticsOptions(),
            DataApiTraderIngestion = configuration.GetSection("DataApiTraderIngestion").Get<DataApiTraderIngestionOptions>() ?? new DataApiTraderIngestionOptions(),
            Watchlist = watchlist,
            PaperTrading = configuration.GetSection("PaperTrading").Get<PaperTradingOptions>() ?? new PaperTradingOptions(),
            LiveTrading = configuration.GetSection("LiveTrading").Get<LiveTradingOptions>() ?? new LiveTradingOptions(),
            Dashboard = configuration.GetSection("Dashboard").Get<DashboardOptions>() ?? new DashboardOptions(),
            Analytics = configuration.GetSection("Analytics").Get<AnalyticsOptions>() ?? new AnalyticsOptions(),
            TraderDiscovery = configuration.GetSection("TraderDiscovery").Get<TraderDiscoveryOptions>() ?? new TraderDiscoveryOptions(),
            GammaMarketIngestion = configuration.GetSection("GammaMarketIngestion").Get<GammaMarketIngestionOptions>() ?? new GammaMarketIngestionOptions(),
            BtcUpDown5mStrategy = configuration.GetSection("BtcUpDown5mStrategy").Get<BtcUpDown5mStrategyOptions>() ?? new BtcUpDown5mStrategyOptions(),
            CoinbaseExchange = configuration.GetSection("CoinbaseExchange").Get<CoinbaseExchangeOptions>() ?? new CoinbaseExchangeOptions(),
            BinanceBtcUsdReference = configuration.GetSection("BinanceBtcUsdReference").Get<BinanceBtcUsdReferenceOptions>() ?? new BinanceBtcUsdReferenceOptions(),
            BinanceCryptoReference = binanceCryptoReference,
            BtcUpDown5mOddsArchive = configuration.GetSection("BtcUpDown5mOddsArchive").Get<BtcUpDown5mOddsArchiveOptions>() ?? new BtcUpDown5mOddsArchiveOptions(),
            CryptoUpDown5mOddsArchive = cryptoUpDown5mOddsArchive,
            ChainlinkBtcUsdDiagnostics = configuration.GetSection("ChainlinkBtcUsdDiagnostics").Get<ChainlinkBtcUsdDiagnosticsOptions>() ?? new ChainlinkBtcUsdDiagnosticsOptions(),
            OnChainIngestion = configuration.GetSection("OnChainIngestion").Get<OnChainIngestionOptions>() ?? new OnChainIngestionOptions(),
            Ipc = configuration.GetSection("Ipc").Get<IpcOptions>() ?? new IpcOptions(),
            Storage = configuration.GetSection("Storage").Get<StorageOptions>() ?? new StorageOptions()
        };
    }

    private static BinanceCryptoReferenceOptions NormalizeBinanceCryptoReferenceOptions(BinanceCryptoReferenceOptions options)
    {
        return new BinanceCryptoReferenceOptions
        {
            Enabled = options.Enabled,
            CombinedStreamBaseUrl = options.CombinedStreamBaseUrl,
            AssetSymbols = NormalizeAssetSymbols(options.AssetSymbols),
            StaleAfterSeconds = options.StaleAfterSeconds,
            ReconnectBaseDelaySeconds = options.ReconnectBaseDelaySeconds,
            ReconnectMaxDelaySeconds = options.ReconnectMaxDelaySeconds,
            ReceiveBufferBytes = options.ReceiveBufferBytes
        };
    }

    private static CryptoUpDown5mOddsArchiveOptions NormalizeCryptoUpDown5mOddsArchiveOptions(CryptoUpDown5mOddsArchiveOptions options)
    {
        return new CryptoUpDown5mOddsArchiveOptions
        {
            Enabled = options.Enabled,
            AssetSymbols = NormalizeAssetSymbols(options.AssetSymbols),
            PollIntervalSeconds = options.PollIntervalSeconds,
            MaxMarketsPerCycle = options.MaxMarketsPerCycle,
            MaxOrderBookAgeMilliseconds = options.MaxOrderBookAgeMilliseconds,
            RestFallbackEnabled = options.RestFallbackEnabled
        };
    }

    private static List<string> NormalizeAssetSymbols(IEnumerable<string> assetSymbols)
    {
        return assetSymbols
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Select(symbol => symbol.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
