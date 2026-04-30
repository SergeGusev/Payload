using PolyCopyTrader.Service;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Polymarket.Auth;
using PolyCopyTrader.Polymarket.OnChain;
using PolyCopyTrader.Service.Analytics;
using PolyCopyTrader.Service.Configuration;
using PolyCopyTrader.Service.Control;
using PolyCopyTrader.Service.LiveTrading;
using PolyCopyTrader.Service.MarketData;
using PolyCopyTrader.Service.OnChain;
using PolyCopyTrader.Service.PaperTrading;
using PolyCopyTrader.Service.Polymarket;
using PolyCopyTrader.Service.Scanning;
using PolyCopyTrader.Service.Signals;
using PolyCopyTrader.Service.Startup;
using PolyCopyTrader.Service.TraderDiscovery;
using PolyCopyTrader.Storage;
using PolyCopyTrader.Strategy;
using Serilog;
using Serilog.Events;

var builder = Host.CreateApplicationBuilder(args);
var appConfiguration = AppConfigurationLoader.Load(builder.Configuration);
AppOptionsValidator.ValidateAndThrow(appConfiguration);

if (args.Contains("--print-config", StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine(AppOptionsValidator.ToSanitizedSummary(appConfiguration));
    return;
}

var logsDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
Directory.CreateDirectory(logsDirectory);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.File(
        Path.Combine(logsDirectory, "polycopytrader-service-.log"),
        rollingInterval: RollingInterval.Day)
    .CreateLogger();

if (!StorageConnectionResolver.IsConfigured(appConfiguration.Storage))
{
    var message =
        $"PolyCopyTrader.Service requires PostgreSQL storage. Set {appConfiguration.Storage.ConnectionStringEnvironmentVariable} or Storage:ConnectionString.";
    Log.Fatal(message);
    await Log.CloseAndFlushAsync();
    throw new InvalidOperationException(message);
}

builder.Services.AddSerilog(Log.Logger, dispose: true);
builder.Services.AddSingleton(appConfiguration);
builder.Services.AddSingleton(appConfiguration.Bot);
builder.Services.AddSingleton(appConfiguration.Risk);
builder.Services.AddSingleton(appConfiguration.Execution);
builder.Services.AddSingleton(appConfiguration.Signal);
builder.Services.AddSingleton(appConfiguration.Polymarket);
builder.Services.AddSingleton(appConfiguration.PolymarketAuth);
builder.Services.AddSingleton(appConfiguration.MarketDataWebSocket);
builder.Services.AddSingleton(appConfiguration.Watchlist);
builder.Services.AddSingleton(appConfiguration.PaperTrading);
builder.Services.AddSingleton(appConfiguration.LiveTrading);
builder.Services.AddSingleton(appConfiguration.Dashboard);
builder.Services.AddSingleton(appConfiguration.Analytics);
builder.Services.AddSingleton(appConfiguration.TraderDiscovery);
builder.Services.AddSingleton(appConfiguration.OnChainIngestion);
builder.Services.AddSingleton(appConfiguration.Ipc);
builder.Services.AddSingleton(appConfiguration.Storage);
builder.Services.AddWindowsService(options => options.ServiceName = "PolyCopyTrader.Service");
builder.Services.AddSingleton<PostgresConnectionFactory>();
builder.Services.AddSingleton<IStorageSchemaInitializer, PostgresSchemaInitializer>();
builder.Services.AddSingleton<IAppRepository, PostgresAppRepository>();

builder.Services.AddSingleton<IPolymarketApiErrorSink, RepositoryPolymarketApiErrorSink>();
builder.Services.AddSingleton<IPolymarketHttpLogSink, RepositoryPolymarketHttpLogSink>();
builder.Services.AddSingleton(PolymarketSecretProviderFactory.Create(appConfiguration.PolymarketAuth));
builder.Services.AddSingleton<PolymarketL2HmacSigner>();
builder.Services.AddSingleton<PolymarketAuthHeaderFactory>();
builder.Services.AddSingleton<IPolymarketAuthService, PolymarketAuthReadinessService>();
builder.Services.AddSingleton<OrderAmountCalculator>();
builder.Services.AddSingleton<ClobV2OrderBuilder>();
builder.Services.AddSingleton<ClobV2OrderSigner>();
builder.Services.AddSingleton<ClobV2OrderPayloadSerializer>();
builder.Services.AddHttpClient<IPolymarketDataApiClient, PolymarketDataApiClient>()
    .ConfigurePrimaryHttpMessageHandler(() => CreatePolymarketHttpHandler(appConfiguration.Polymarket));
builder.Services.AddHttpClient<IPolymarketGammaClient, PolymarketGammaClient>()
    .ConfigurePrimaryHttpMessageHandler(() => CreatePolymarketHttpHandler(appConfiguration.Polymarket));
builder.Services.AddHttpClient<IPolymarketClobPublicClient, PolymarketClobPublicClient>()
    .ConfigurePrimaryHttpMessageHandler(() => CreatePolymarketHttpHandler(appConfiguration.Polymarket));
builder.Services.AddHttpClient<IPolymarketGeoClient, PolymarketGeoClient>()
    .ConfigurePrimaryHttpMessageHandler(() => CreatePolymarketHttpHandler(appConfiguration.Polymarket));
builder.Services.AddHttpClient<IPolymarketTradingClient, PolymarketTradingClient>()
    .ConfigurePrimaryHttpMessageHandler(() => CreatePolymarketHttpHandler(appConfiguration.Polymarket));
builder.Services.AddHttpClient<IPolygonRpcClient, PolygonRpcClient>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(2);
});
builder.Services.AddSingleton<ILeaderTradeCandidateQueue, InMemoryLeaderTradeCandidateQueue>();
builder.Services.AddSingleton<IWatchlistScanner, WatchlistScanner>();
builder.Services.AddSingleton<IRiskEngine, DefaultRiskEngine>();
builder.Services.AddSingleton<ISignalEngine, DefaultSignalEngine>();
builder.Services.AddSingleton<IPaperTradingEngine, DefaultPaperTradingEngine>();
builder.Services.AddSingleton<ISignalProcessor, SignalProcessor>();
builder.Services.AddSingleton<IMarketDataCache, MarketDataCache>();
builder.Services.AddSingleton<IRelevantMarketAssetProvider, RelevantMarketAssetProvider>();
builder.Services.AddSingleton<IPaperTradingMarketDataUpdater, PaperTradingMarketDataUpdater>();
builder.Services.AddSingleton<IPaperTradingProcessor, PaperTradingProcessor>();
builder.Services.AddSingleton<ILiveTradingProcessor, LiveTradingProcessor>();
builder.Services.AddSingleton<ITraderDiscoveryProcessor, TraderDiscoveryProcessor>();
builder.Services.AddSingleton<IOnChainIngestionProcessor, OnChainIngestionProcessor>();
builder.Services.AddSingleton<IOnChainMarketEnrichmentProcessor, OnChainMarketEnrichmentProcessor>();
builder.Services.AddSingleton<ServiceControlState>();
builder.Services.AddHostedService<StartupSafetyCheckService>();
builder.Services.AddHostedService<BotWorker>();
builder.Services.AddHostedService<LocalControlServer>();
builder.Services.AddHostedService<OnChainIngestionWorker>();
builder.Services.AddHostedService<OnChainMarketEnrichmentWorker>();
builder.Services.AddHostedService<OnChainPositionRefreshWorker>();
builder.Services.AddHostedService<OnChainPerformanceRefreshWorker>();
builder.Services.AddHostedService<MarketDataWebSocketService>();
builder.Services.AddHostedService<DailyReportWorker>();

try
{
    Log.Information("Starting PolyCopyTrader service host.");
    Log.Information("Configuration summary:{NewLine}{ConfigSummary}", Environment.NewLine, AppOptionsValidator.ToSanitizedSummary(appConfiguration));
    var host = builder.Build();
    await host.Services.GetRequiredService<IStorageSchemaInitializer>().InitializeAsync();
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "PolyCopyTrader service terminated unexpectedly.");
}
finally
{
    await Log.CloseAndFlushAsync();
}

static HttpMessageHandler CreatePolymarketHttpHandler(PolymarketOptions options)
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
