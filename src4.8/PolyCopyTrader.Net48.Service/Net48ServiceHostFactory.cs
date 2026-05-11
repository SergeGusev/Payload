using Microsoft.Extensions.Configuration;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Polymarket.Auth;
using PolyCopyTrader.Service.Analytics;
using PolyCopyTrader.Service.Configuration;
using PolyCopyTrader.Service.Control;
using PolyCopyTrader.Service.DataApiTraderActivity;
using PolyCopyTrader.Service.Diagnostics;
using PolyCopyTrader.Service.ExternalPrices;
using PolyCopyTrader.Service.GammaMarkets;
using PolyCopyTrader.Service.LiveTrading;
using PolyCopyTrader.Service.MarketData;
using PolyCopyTrader.Service.OnChain;
using PolyCopyTrader.Service.PaperTrading;
using PolyCopyTrader.Service.Polymarket;
using PolyCopyTrader.Service.Scanning;
using PolyCopyTrader.Service.Signals;
using PolyCopyTrader.Service.Startup;
using PolyCopyTrader.Service.Strategies;
using PolyCopyTrader.Service.TraderDiscovery;
using PolyCopyTrader.Storage;
using PolyCopyTrader.Strategy;
using Serilog;
using Serilog.Events;

namespace PolyCopyTrader.Service;

internal static class Net48ServiceHostFactory
{
    public static AppConfiguration LoadAppConfiguration()
    {
        var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ??
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ??
            "Production";

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile("appsettings." + environment + ".json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .AddEnvironmentVariables("POLYCOPYTRADER_")
            .Build();

        return AppConfigurationLoader.Load(configuration);
    }

    public static IHost Build(string[] args)
    {
        var appConfiguration = LoadAppConfiguration();
        AppOptionsValidator.ValidateAndThrow(appConfiguration);

        if (!StorageConnectionResolver.IsConfigured(appConfiguration.Storage))
        {
            throw new InvalidOperationException(
                "PolyCopyTrader.Net48.Service requires PostgreSQL storage. Set " +
                appConfiguration.Storage.ConnectionStringEnvironmentVariable +
                " or Storage:ConnectionString.");
        }

        ConfigureSerilog();

        return Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration(builder =>
            {
                builder.Sources.Clear();
                builder.SetBasePath(AppContext.BaseDirectory)
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                    .AddEnvironmentVariables()
                    .AddEnvironmentVariables("POLYCOPYTRADER_");
            })
            .UseSerilog(Log.Logger, dispose: true)
            .ConfigureServices(services => RegisterServices(services, appConfiguration))
            .Build();
    }

    private static void ConfigureSerilog()
    {
        var logsDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logsDirectory);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(logsDirectory, "polycopytrader-net48-service-.log"),
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: 50_000_000,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: 30)
            .CreateLogger();
    }

    private static void RegisterServices(IServiceCollection services, AppConfiguration appConfiguration)
    {
        services.AddSingleton(appConfiguration);
        services.AddSingleton(appConfiguration.Bot);
        services.AddSingleton(appConfiguration.Risk);
        services.AddSingleton(appConfiguration.Execution);
        services.AddSingleton(appConfiguration.Signal);
        services.AddSingleton(appConfiguration.Polymarket);
        services.AddSingleton(appConfiguration.PolymarketHttpLogging);
        services.AddSingleton(appConfiguration.PolymarketAuth);
        services.AddSingleton(appConfiguration.MarketDataWebSocket);
        services.AddSingleton(appConfiguration.BtcOrderBookLagDiagnostics);
        services.AddSingleton(appConfiguration.Watchlist);
        services.AddSingleton(appConfiguration.PaperTrading);
        services.AddSingleton(appConfiguration.LiveTrading);
        services.AddSingleton(appConfiguration.Dashboard);
        services.AddSingleton(appConfiguration.Analytics);
        services.AddSingleton(appConfiguration.TraderDiscovery);
        services.AddSingleton(appConfiguration.GammaMarketIngestion);
        services.AddSingleton(appConfiguration.BtcUpDown5mStrategy);
        services.AddSingleton(appConfiguration.CoinbaseExchange);
        services.AddSingleton(appConfiguration.BinanceBtcUsdReference);
        services.AddSingleton(appConfiguration.BinanceCryptoReference);
        services.AddSingleton(appConfiguration.BtcUpDown5mOddsArchive);
        services.AddSingleton(appConfiguration.CryptoUpDown5mOddsArchive);
        services.AddSingleton(appConfiguration.ChainlinkBtcUsdDiagnostics);
        services.AddSingleton(appConfiguration.MarketTradeDiagnostics);
        services.AddSingleton(appConfiguration.DataApiTraderIngestion);
        services.AddSingleton(appConfiguration.OnChainIngestion);
        services.AddSingleton(appConfiguration.Ipc);
        services.AddSingleton(appConfiguration.Storage);

        services.AddSingleton<PostgresConnectionFactory>();
        services.AddSingleton<IStorageSchemaInitializer, PostgresSchemaInitializer>();
        services.AddSingleton<IAppRepository, PostgresAppRepository>();

        services.AddSingleton<IPolymarketApiErrorSink, RepositoryPolymarketApiErrorSink>();
        services.AddSingleton<IPolymarketHttpLogSink, RepositoryPolymarketHttpLogSink>();
        services.AddSingleton<IPolymarketAuthService, DisabledPolymarketAuthService>();
        services.AddSingleton<IPolymarketTradingClient, DisabledPolymarketTradingClient>();
        services.AddHttpClient<IPolymarketDataApiClient, PolymarketDataApiClient>()
            .ConfigurePrimaryHttpMessageHandler(() => CreatePolymarketHttpHandler(appConfiguration.Polymarket));
        services.AddHttpClient<IPolymarketGammaClient, PolymarketGammaClient>()
            .ConfigurePrimaryHttpMessageHandler(() => CreatePolymarketHttpHandler(appConfiguration.Polymarket));
        services.AddHttpClient<IPolymarketClobPublicClient, PolymarketClobPublicClient>()
            .ConfigurePrimaryHttpMessageHandler(() => CreatePolymarketHttpHandler(appConfiguration.Polymarket));
        services.AddHttpClient<IPolymarketGeoClient, PolymarketGeoClient>()
            .ConfigurePrimaryHttpMessageHandler(() => CreatePolymarketHttpHandler(appConfiguration.Polymarket));

        services.AddSingleton<ILeaderTradeCandidateQueue, InMemoryLeaderTradeCandidateQueue>();
        services.AddSingleton<IWatchlistScanner, WatchlistScanner>();
        services.AddSingleton<IRiskEngine, DefaultRiskEngine>();
        services.AddSingleton<ISignalEngine, DefaultSignalEngine>();
        services.AddSingleton<IPaperTradingEngine, DefaultPaperTradingEngine>();
        services.AddSingleton<ISignalProcessor, SignalProcessor>();
        services.AddSingleton<IMarketDataCache, MarketDataCache>();
        services.AddSingleton<IActiveMarketAssetSubscriptionRegistry, ActiveMarketAssetSubscriptionRegistry>();
        services.AddSingleton<IRelevantMarketAssetProvider, RelevantMarketAssetProvider>();
        services.AddSingleton<IBtcUsdReferencePriceCache>(_ => new BtcUsdReferencePriceCache(appConfiguration.BinanceBtcUsdReference));
        services.AddSingleton<BinanceBtcUsdTradeStreamService>();
        services.AddSingleton<IBtcUsdReferencePriceClient>(sp => sp.GetRequiredService<BinanceBtcUsdTradeStreamService>());
        services.AddSingleton<BinanceCryptoReferenceTradeStreamService>();
        services.AddSingleton<ICryptoReferencePriceClient>(sp => sp.GetRequiredService<BinanceCryptoReferenceTradeStreamService>());
        services.AddHttpClient<ChainlinkBtcUsdCorrelationWorker>();
        services.AddSingleton<BtcOrderBookLagDiagnosticService>();
        services.AddSingleton<IBtcOrderBookLagDiagnosticService>(sp => sp.GetRequiredService<BtcOrderBookLagDiagnosticService>());
        services.AddSingleton<MarketTradeTickDiagnosticService>();
        services.AddSingleton<IMarketTradeTickDiagnosticService>(sp => sp.GetRequiredService<MarketTradeTickDiagnosticService>());
        services.AddSingleton<IExposureSnapshotCache, ExposureSnapshotCache>();
        services.AddSingleton<IPaperTradingMarketDataUpdater, PaperTradingMarketDataUpdater>();
        services.AddSingleton<IPaperTradingProcessor, PaperTradingProcessor>();
        services.AddSingleton<IPaperSettlementProcessor, PaperSettlementProcessor>();
        services.AddSingleton<ILeaderActivityExitProcessor, LeaderActivityExitProcessor>();
        services.AddSingleton<ILiveTradingProcessor, DisabledLiveTradingProcessor>();
        services.AddSingleton<ITraderDiscoveryProcessor, TraderDiscoveryProcessor>();
        services.AddSingleton<IGammaMarketIngestionProcessor, GammaMarketIngestionProcessor>();
        services.AddSingleton<IStrategyStateProvider, StrategyStateProvider>();
        services.AddSingleton<IBtcUpDown5mPaperStrategyProcessor, BtcUpDown5mPaperStrategyProcessor>();
        services.AddSingleton<IBtcUpDown5mOddsArchiveProcessor, BtcUpDown5mOddsArchiveProcessor>();
        services.AddSingleton<ICryptoUpDown5mOddsArchiveProcessor, CryptoUpDown5mOddsArchiveProcessor>();
        services.AddSingleton<IDataApiTraderActivityIngestionProcessor, DataApiTraderActivityIngestionProcessor>();
        services.AddSingleton<IOnChainIngestionProcessor, DisabledOnChainIngestionProcessor>();
        services.AddSingleton<IOnChainMarketEnrichmentProcessor, DisabledOnChainMarketEnrichmentProcessor>();
        services.AddSingleton<ServiceControlState>();

        services.AddHostedService<StartupSafetyCheckService>();
        services.AddHostedService<PolymarketHttpLogRetentionWorker>();
        services.AddHostedService<BotWorker>();
        services.AddHostedService<PaperTradingWorker>();
        services.AddHostedService<LocalControlServer>();
        services.AddHostedService<GammaMarketIngestionWorker>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<BtcOrderBookLagDiagnosticService>());
        services.AddHostedService(sp => sp.GetRequiredService<BinanceBtcUsdTradeStreamService>());
        services.AddHostedService(sp => sp.GetRequiredService<BinanceCryptoReferenceTradeStreamService>());
        services.AddHostedService<ChainlinkBtcUsdCorrelationWorker>();
        services.AddHostedService<BtcUpDown5mOrderBookRefreshWorker>();
        services.AddHostedService<BtcUpDown5mPaperStrategyWorker>();
        services.AddHostedService<BtcUpDown5mOddsArchiveWorker>();
        services.AddHostedService<CryptoUpDown5mOddsArchiveWorker>();
        services.AddHostedService<DataApiTraderActivityIngestionWorker>();
        services.AddHostedService<DataApiTraderActivitySyncWorker>();
        services.AddHostedService<DataApiTraderRatingRefreshWorker>();
        services.AddHostedService<MarketDataWebSocketService>();
        services.AddHostedService<PaperAccountingWorker>();
        services.AddHostedService<LeaderActivityExitWorker>();
        services.AddHostedService<DailyReportWorker>();
    }

    private static HttpMessageHandler CreatePolymarketHttpHandler(PolymarketOptions options)
    {
        var handler = new HttpClientHandler();
        if (!PolymarketCertificatePinning.HasPins(options))
        {
            return handler;
        }

        handler.ServerCertificateCustomValidationCallback = (request, certificate, _, sslPolicyErrors) =>
        {
            var result = PolymarketCertificatePinning.ValidateServerCertificate(
                request.RequestUri,
                certificate,
                sslPolicyErrors,
                options);

            if (!result.Accepted)
            {
                Log.Warning(
                    "Polymarket TLS certificate rejected for {Host}: {Message}",
                    request.RequestUri?.Host ?? "<unknown>",
                    result.Message);
            }

            return result.Accepted;
        };

        return handler;
    }
}
