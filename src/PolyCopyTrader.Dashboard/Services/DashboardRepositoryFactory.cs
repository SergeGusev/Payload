using Microsoft.Extensions.Configuration;
using Npgsql;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket.Auth;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Dashboard.Services;

public static class DashboardRepositoryFactory
{
    public static DashboardRuntime Create(DashboardDatabaseSource databaseSource = DashboardDatabaseSource.Local)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();

        var appConfiguration = new AppConfiguration
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
            BtcOrderBookLagDiagnostics = configuration.GetSection("BtcOrderBookLagDiagnostics").Get<BtcOrderBookLagDiagnosticsOptions>() ?? new BtcOrderBookLagDiagnosticsOptions(),
            DataApiTraderIngestion = configuration.GetSection("DataApiTraderIngestion").Get<DataApiTraderIngestionOptions>() ?? new DataApiTraderIngestionOptions(),
            Dashboard = configuration.GetSection("Dashboard").Get<DashboardOptions>() ?? new DashboardOptions(),
            Storage = configuration.GetSection("Storage").Get<StorageOptions>() ?? new StorageOptions(),
            PaperTrading = configuration.GetSection("PaperTrading").Get<PaperTradingOptions>() ?? new PaperTradingOptions(),
            LiveTrading = configuration.GetSection("LiveTrading").Get<LiveTradingOptions>() ?? new LiveTradingOptions(),
            Watchlist = configuration.GetSection("Watchlist").Get<WatchlistOptions>() ?? new WatchlistOptions(),
            Analytics = configuration.GetSection("Analytics").Get<AnalyticsOptions>() ?? new AnalyticsOptions(),
            TraderDiscovery = configuration.GetSection("TraderDiscovery").Get<TraderDiscoveryOptions>() ?? new TraderDiscoveryOptions(),
            GammaMarketIngestion = configuration.GetSection("GammaMarketIngestion").Get<GammaMarketIngestionOptions>() ?? new GammaMarketIngestionOptions(),
            BtcUpDown5mStrategy = configuration.GetSection("BtcUpDown5mStrategy").Get<BtcUpDown5mStrategyOptions>() ?? new BtcUpDown5mStrategyOptions(),
            CoinbaseExchange = configuration.GetSection("CoinbaseExchange").Get<CoinbaseExchangeOptions>() ?? new CoinbaseExchangeOptions(),
            BinanceBtcUsdReference = configuration.GetSection("BinanceBtcUsdReference").Get<BinanceBtcUsdReferenceOptions>() ?? new BinanceBtcUsdReferenceOptions(),
            BinanceCryptoReference = configuration.GetSection("BinanceCryptoReference").Get<BinanceCryptoReferenceOptions>() ?? new BinanceCryptoReferenceOptions(),
            BtcUpDown5mOddsArchive = configuration.GetSection("BtcUpDown5mOddsArchive").Get<BtcUpDown5mOddsArchiveOptions>() ?? new BtcUpDown5mOddsArchiveOptions(),
            BtcUpDown5mStatistics = configuration.GetSection("BtcUpDown5mStatistics").Get<BtcUpDown5mStatisticsOptions>() ?? new BtcUpDown5mStatisticsOptions(),
            CryptoUpDown5mOddsArchive = configuration.GetSection("CryptoUpDown5mOddsArchive").Get<CryptoUpDown5mOddsArchiveOptions>() ?? new CryptoUpDown5mOddsArchiveOptions(),
            ChainlinkBtcUsdDiagnostics = configuration.GetSection("ChainlinkBtcUsdDiagnostics").Get<ChainlinkBtcUsdDiagnosticsOptions>() ?? new ChainlinkBtcUsdDiagnosticsOptions(),
            OnChainIngestion = configuration.GetSection("OnChainIngestion").Get<OnChainIngestionOptions>() ?? new OnChainIngestionOptions(),
            Ipc = configuration.GetSection("Ipc").Get<IpcOptions>() ?? new IpcOptions()
        };
        if (databaseSource == DashboardDatabaseSource.Remote)
        {
            appConfiguration = WithStorage(
                appConfiguration,
                WithRemoteDatabaseHost(appConfiguration.Storage, DashboardDatabaseSources.RemoteHost));
        }

        IAppRepository repository;
        if (StorageConnectionResolver.IsConfigured(appConfiguration.Storage))
        {
            var connectionFactory = new PostgresConnectionFactory(appConfiguration.Storage);
            repository = new PostgresAppRepository(connectionFactory);
        }
        else
        {
            repository = new NoOpAppRepository();
        }

        var secretProvider = PolymarketSecretProviderFactory.Create(appConfiguration.PolymarketAuth);
        var authService = new PolymarketAuthReadinessService(
            appConfiguration.PolymarketAuth,
            secretProvider,
            new PolymarketL2HmacSigner());

        return new DashboardRuntime(
            repository,
            appConfiguration,
            StorageConnectionResolver.IsConfigured(appConfiguration.Storage),
            authService,
            databaseSource,
            DashboardDatabaseSources.ToDisplayName(databaseSource),
            databaseSource == DashboardDatabaseSource.Remote ? DashboardDatabaseSources.RemoteHost : null);
    }

    private static StorageOptions WithRemoteDatabaseHost(StorageOptions options, string host)
    {
        var connectionString = StorageConnectionResolver.Resolve(options);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return options;
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Host = host
        };

        return new StorageOptions
        {
            Provider = options.Provider,
            ConnectionString = builder.ConnectionString,
            ConnectionStringEnvironmentVariable = options.ConnectionStringEnvironmentVariable,
            RequireConfiguredDatabase = options.RequireConfiguredDatabase
        };
    }

    private static AppConfiguration WithStorage(AppConfiguration configuration, StorageOptions storage)
    {
        return new AppConfiguration
        {
            Bot = configuration.Bot,
            Risk = configuration.Risk,
            Execution = configuration.Execution,
            Signal = configuration.Signal,
            Polymarket = configuration.Polymarket,
            PolymarketHttpLogging = configuration.PolymarketHttpLogging,
            PolymarketAuth = configuration.PolymarketAuth,
            MarketDataWebSocket = configuration.MarketDataWebSocket,
            MarketTradeDiagnostics = configuration.MarketTradeDiagnostics,
            BtcOrderBookLagDiagnostics = configuration.BtcOrderBookLagDiagnostics,
            DataApiTraderIngestion = configuration.DataApiTraderIngestion,
            Watchlist = configuration.Watchlist,
            PaperTrading = configuration.PaperTrading,
            LiveTrading = configuration.LiveTrading,
            Dashboard = configuration.Dashboard,
            Analytics = configuration.Analytics,
            TraderDiscovery = configuration.TraderDiscovery,
            GammaMarketIngestion = configuration.GammaMarketIngestion,
            BtcUpDown5mStrategy = configuration.BtcUpDown5mStrategy,
            CoinbaseExchange = configuration.CoinbaseExchange,
            BinanceBtcUsdReference = configuration.BinanceBtcUsdReference,
            BinanceCryptoReference = configuration.BinanceCryptoReference,
            BtcUpDown5mOddsArchive = configuration.BtcUpDown5mOddsArchive,
            BtcUpDown5mStatistics = configuration.BtcUpDown5mStatistics,
            CryptoUpDown5mOddsArchive = configuration.CryptoUpDown5mOddsArchive,
            ChainlinkBtcUsdDiagnostics = configuration.ChainlinkBtcUsdDiagnostics,
            OnChainIngestion = configuration.OnChainIngestion,
            Ipc = configuration.Ipc,
            Storage = storage
        };
    }
}

public sealed record DashboardRuntime(
    IAppRepository Repository,
    AppConfiguration Configuration,
    bool StorageConfigured,
    IPolymarketAuthService AuthService,
    DashboardDatabaseSource DatabaseSource,
    string DatabaseSourceDisplayName,
    string? DatabaseHost);
