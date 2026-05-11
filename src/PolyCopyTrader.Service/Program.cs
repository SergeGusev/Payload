using System.Globalization;
using PolyCopyTrader.Service;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Polymarket.Auth;
using PolyCopyTrader.Polymarket.OnChain;
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

if (args.Contains("--bootstrap-polymarket-api-credentials", StringComparer.OrdinalIgnoreCase))
{
    var commandConfiguration = LoadCommandConfiguration();
    AppOptionsValidator.ValidateAndThrow(commandConfiguration);
    Environment.ExitCode = await PolymarketApiCredentialBootstrapCommand.ExecuteAsync(
        commandConfiguration,
        Console.Out,
        CancellationToken.None);
    return;
}

if (args.Contains("--binance-sbe-smoke", StringComparer.OrdinalIgnoreCase))
{
    Environment.ExitCode = await BinanceSbeSmokeCommand.ExecuteAsync(
        args,
        Console.Out,
        CancellationToken.None);
    return;
}

if (args.Contains("--btc-source-comparison-csv", StringComparer.OrdinalIgnoreCase))
{
    Environment.ExitCode = await BtcSourceComparisonCsvCommand.ExecuteAsync(
        args,
        Console.Out,
        CancellationToken.None);
    return;
}

var builder = Host.CreateApplicationBuilder(args);
var appConfiguration = AppConfigurationLoader.Load(builder.Configuration);
AppOptionsValidator.ValidateAndThrow(appConfiguration);

if (args.Contains("--print-config", StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine(AppOptionsValidator.ToSanitizedSummary(appConfiguration));
    return;
}

if (args.Contains("--dry-run-signing-smoke", StringComparer.OrdinalIgnoreCase))
{
    Environment.ExitCode = await DryRunSigningSmokeCommand.ExecuteAsync(
        appConfiguration,
        Console.Out,
        CancellationToken.None);
    return;
}

if (args.Contains("--auth-readiness-smoke", StringComparer.OrdinalIgnoreCase))
{
    Environment.ExitCode = await AuthReadinessSmokeCommand.ExecuteAsync(
        appConfiguration,
        Console.Out,
        CancellationToken.None);
    return;
}

if (args.Contains("--clob-authenticated-read-smoke", StringComparer.OrdinalIgnoreCase))
{
    Environment.ExitCode = await ClobAuthenticatedReadSmokeCommand.ExecuteAsync(
        appConfiguration,
        Console.Out,
        CancellationToken.None);
    return;
}

if (args.Contains("--clob-authenticated-trades-report", StringComparer.OrdinalIgnoreCase))
{
    Environment.ExitCode = await ClobAuthenticatedReadSmokeCommand.ExecuteReportAsync(
        appConfiguration,
        Console.Out,
        CancellationToken.None);
    return;
}

if (args.Contains("--clob-authenticated-open-orders-report", StringComparer.OrdinalIgnoreCase))
{
    Environment.ExitCode = await ClobAuthenticatedReadSmokeCommand.ExecuteOpenOrdersReportAsync(
        appConfiguration,
        Console.Out,
        CancellationToken.None);
    return;
}

if (args.Contains("--clob-cancel-all-smoke", StringComparer.OrdinalIgnoreCase))
{
    Environment.ExitCode = await ClobCancelAllSmokeCommand.ExecuteAsync(
        appConfiguration,
        Console.Out,
        CancellationToken.None);
    return;
}

if (args.Contains("--clob-min-live-order-smoke", StringComparer.OrdinalIgnoreCase))
{
    Environment.ExitCode = await ClobMinimumLiveOrderSmokeCommand.ExecuteAsync(
        appConfiguration,
        args,
        Console.Out,
        CancellationToken.None);
    return;
}

if (TryGetOptionValue(args, "--set-paper-stake-usd") is { } paperStakeValue)
{
    if (!decimal.TryParse(paperStakeValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var paperStakeUsd))
    {
        Console.Error.WriteLine("--set-paper-stake-usd must be a decimal value using invariant culture, for example 5.00.");
        Environment.ExitCode = 1;
        return;
    }

    Environment.ExitCode = await StrategyStakeAdminCommand.ExecuteAsync(
        appConfiguration,
        paperStakeUsd,
        Console.Out,
        CancellationToken.None);
    return;
}

if (TryGetOptionValue(args, "--set-stake-multiplier") is { } stakeMultiplierValue)
{
    if (!decimal.TryParse(stakeMultiplierValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var stakeMultiplier))
    {
        Console.Error.WriteLine("--set-stake-multiplier must be a decimal value using invariant culture, for example 1.00.");
        Environment.ExitCode = 1;
        return;
    }

    Environment.ExitCode = await StrategyStakeAdminCommand.ExecuteAsync(
        appConfiguration,
        stakeMultiplier,
        stakeMultiplier,
        Console.Out,
        CancellationToken.None);
    return;
}

if (TryGetOptionValue(args, "--set-live-stakes-only-code") is { } liveStakesOnlyCode)
{
    Environment.ExitCode = await StrategyStakeAdminCommand.ExecuteLiveStakesOnlyAsync(
        appConfiguration,
        liveStakesOnlyCode,
        Console.Out,
        CancellationToken.None);
    return;
}

if (args.Contains("--disable-all-live-stakes", StringComparer.OrdinalIgnoreCase))
{
    Environment.ExitCode = await StrategyStakeAdminCommand.DisableAllLiveStakesAsync(
        appConfiguration,
        Console.Out,
        CancellationToken.None);
    return;
}

if (args.Contains("--print-live-shadow-state", StringComparer.OrdinalIgnoreCase))
{
    Environment.ExitCode = await StrategyStakeAdminCommand.PrintLiveShadowStateAsync(
        appConfiguration,
        Console.Out,
        CancellationToken.None);
    return;
}

if (args.Contains("--print-live-shadow-exchange-status", StringComparer.OrdinalIgnoreCase))
{
    Environment.ExitCode = await StrategyStakeAdminCommand.PrintLiveShadowExchangeStatusAsync(
        appConfiguration,
        Console.Out,
        CancellationToken.None);
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
        rollingInterval: RollingInterval.Day,
        fileSizeLimitBytes: 50_000_000,
        rollOnFileSizeLimit: true,
        retainedFileCountLimit: 30)
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
builder.Services.AddSingleton(appConfiguration.PolymarketHttpLogging);
builder.Services.AddSingleton(appConfiguration.PolymarketAuth);
builder.Services.AddSingleton(appConfiguration.MarketDataWebSocket);
builder.Services.AddSingleton(appConfiguration.BtcOrderBookLagDiagnostics);
builder.Services.AddSingleton(appConfiguration.Watchlist);
builder.Services.AddSingleton(appConfiguration.PaperTrading);
builder.Services.AddSingleton(appConfiguration.LiveTrading);
builder.Services.AddSingleton(appConfiguration.Dashboard);
builder.Services.AddSingleton(appConfiguration.Analytics);
    builder.Services.AddSingleton(appConfiguration.TraderDiscovery);
    builder.Services.AddSingleton(appConfiguration.GammaMarketIngestion);
    builder.Services.AddSingleton(appConfiguration.BtcUpDown5mStrategy);
    builder.Services.AddSingleton(appConfiguration.CoinbaseExchange);
    builder.Services.AddSingleton(appConfiguration.BinanceBtcUsdReference);
    builder.Services.AddSingleton(appConfiguration.BinanceCryptoReference);
    builder.Services.AddSingleton(appConfiguration.BtcUpDown5mOddsArchive);
    builder.Services.AddSingleton(appConfiguration.CryptoUpDown5mOddsArchive);
    builder.Services.AddSingleton(appConfiguration.ChainlinkBtcUsdDiagnostics);
    builder.Services.AddSingleton(appConfiguration.MarketTradeDiagnostics);
builder.Services.AddSingleton(appConfiguration.DataApiTraderIngestion);
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
builder.Services.AddSingleton<IActiveMarketAssetSubscriptionRegistry, ActiveMarketAssetSubscriptionRegistry>();
builder.Services.AddSingleton<IRelevantMarketAssetProvider, RelevantMarketAssetProvider>();
builder.Services.AddSingleton<IBtcUsdReferencePriceCache>(_ => new BtcUsdReferencePriceCache(appConfiguration.BinanceBtcUsdReference));
builder.Services.AddSingleton<BinanceBtcUsdTradeStreamService>();
builder.Services.AddSingleton<IBtcUsdReferencePriceClient>(sp => sp.GetRequiredService<BinanceBtcUsdTradeStreamService>());
builder.Services.AddSingleton<BinanceCryptoReferenceTradeStreamService>();
builder.Services.AddSingleton<ICryptoReferencePriceClient>(sp => sp.GetRequiredService<BinanceCryptoReferenceTradeStreamService>());
builder.Services.AddHttpClient<ChainlinkBtcUsdCorrelationWorker>();
builder.Services.AddSingleton<BtcOrderBookLagDiagnosticService>();
builder.Services.AddSingleton<IBtcOrderBookLagDiagnosticService>(sp => sp.GetRequiredService<BtcOrderBookLagDiagnosticService>());
builder.Services.AddSingleton<MarketTradeTickDiagnosticService>();
builder.Services.AddSingleton<IMarketTradeTickDiagnosticService>(sp => sp.GetRequiredService<MarketTradeTickDiagnosticService>());
builder.Services.AddSingleton<IExposureSnapshotCache, ExposureSnapshotCache>();
builder.Services.AddSingleton<IPaperTradingMarketDataUpdater, PaperTradingMarketDataUpdater>();
builder.Services.AddSingleton<ConservativePaperGtdFillEstimator>();
builder.Services.AddSingleton<IPaperTradingProcessor, PaperTradingProcessor>();
builder.Services.AddSingleton<IPaperSettlementProcessor, PaperSettlementProcessor>();
builder.Services.AddSingleton<ILeaderActivityExitProcessor, LeaderActivityExitProcessor>();
builder.Services.AddSingleton<ILiveTradingProcessor, LiveTradingProcessor>();
builder.Services.AddSingleton<ITraderDiscoveryProcessor, TraderDiscoveryProcessor>();
builder.Services.AddSingleton<IGammaMarketIngestionProcessor, GammaMarketIngestionProcessor>();
builder.Services.AddSingleton<IStrategyStateProvider, StrategyStateProvider>();
builder.Services.AddSingleton<IBtcUpDown5mPaperStrategyProcessor, BtcUpDown5mPaperStrategyProcessor>();
builder.Services.AddSingleton<IBtcUpDown5mOddsArchiveProcessor, BtcUpDown5mOddsArchiveProcessor>();
builder.Services.AddSingleton<ICryptoUpDown5mOddsArchiveProcessor, CryptoUpDown5mOddsArchiveProcessor>();
builder.Services.AddSingleton<IDataApiTraderActivityIngestionProcessor, DataApiTraderActivityIngestionProcessor>();
builder.Services.AddSingleton<IOnChainIngestionProcessor, OnChainIngestionProcessor>();
builder.Services.AddSingleton<IOnChainTradeCaptureProcessor, OnChainTradeCaptureProcessor>();
builder.Services.AddSingleton<IOnChainPaperSignalProcessor, OnChainPaperSignalProcessor>();
builder.Services.AddSingleton<IOnChainMarketEnrichmentProcessor, OnChainMarketEnrichmentProcessor>();
builder.Services.AddSingleton<IOnChainSignalCandidateProcessor, OnChainSignalCandidateProcessor>();
builder.Services.AddSingleton<ServiceControlState>();
builder.Services.AddHostedService<StartupSafetyCheckService>();
builder.Services.AddHostedService<PolymarketHttpLogRetentionWorker>();
builder.Services.AddHostedService<BotWorker>();
builder.Services.AddHostedService<PaperTradingWorker>();
builder.Services.AddHostedService<LiveTradingMaintenanceWorker>();
builder.Services.AddHostedService<LocalControlServer>();
builder.Services.AddHostedService<GammaMarketIngestionWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<BtcOrderBookLagDiagnosticService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<BinanceBtcUsdTradeStreamService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<BinanceCryptoReferenceTradeStreamService>());
builder.Services.AddHostedService<ChainlinkBtcUsdCorrelationWorker>();
builder.Services.AddHostedService<BtcUpDown5mOrderBookRefreshWorker>();
builder.Services.AddHostedService<BtcUpDown5mPaperStrategyWorker>();
builder.Services.AddHostedService<BtcUpDown5mOddsArchiveWorker>();
builder.Services.AddHostedService<CryptoUpDown5mOddsArchiveWorker>();
builder.Services.AddHostedService<DataApiTraderActivityIngestionWorker>();
builder.Services.AddHostedService<DataApiTraderActivitySyncWorker>();
builder.Services.AddHostedService<DataApiTraderRatingRefreshWorker>();
// Temporarily paused: on-chain blockchain download and derived-data processing workers.
// Existing PostgreSQL data is left intact; uncomment these registrations to resume.
// builder.Services.AddHostedService<OnChainIngestionWorker>();
// builder.Services.AddHostedService<OnChainMarketEnrichmentWorker>();
// builder.Services.AddHostedService<OnChainActivityRefreshWorker>();
// builder.Services.AddHostedService<OnChainPositionRefreshWorker>();
// builder.Services.AddHostedService<OnChainPerformanceRefreshWorker>();
// builder.Services.AddHostedService<OnChainCategoryPerformanceRefreshWorker>();
// builder.Services.AddHostedService<OnChainSignalCandidateWorker>();
builder.Services.AddHostedService<OnChainTradeCaptureWorker>();
builder.Services.AddHostedService<OnChainPaperSignalWorker>();
builder.Services.AddHostedService<MarketDataWebSocketService>();
builder.Services.AddHostedService<PaperAccountingWorker>();
builder.Services.AddHostedService<LeaderActivityExitWorker>();
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

static string? TryGetOptionValue(string[] args, string name)
{
    for (var index = 0; index < args.Length; index++)
    {
        if (!string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        return index + 1 < args.Length ? args[index + 1] : string.Empty;
    }

    return null;
}

static AppConfiguration LoadCommandConfiguration()
{
    var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ??
        Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ??
        "Production";

    var configuration = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
        .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: false)
        .AddEnvironmentVariables()
        .Build();

    return AppConfigurationLoader.Load(configuration);
}
