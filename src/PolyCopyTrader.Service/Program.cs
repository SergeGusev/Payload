using PolyCopyTrader.Service;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.Configuration;
using PolyCopyTrader.Service.PaperTrading;
using PolyCopyTrader.Service.Polymarket;
using PolyCopyTrader.Service.Scanning;
using PolyCopyTrader.Service.Signals;
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

builder.Services.AddSerilog(Log.Logger, dispose: true);
builder.Services.AddSingleton(appConfiguration);
builder.Services.AddSingleton(appConfiguration.Bot);
builder.Services.AddSingleton(appConfiguration.Risk);
builder.Services.AddSingleton(appConfiguration.Execution);
builder.Services.AddSingleton(appConfiguration.Signal);
builder.Services.AddSingleton(appConfiguration.Polymarket);
builder.Services.AddSingleton(appConfiguration.Watchlist);
builder.Services.AddSingleton(appConfiguration.PaperTrading);
builder.Services.AddSingleton(appConfiguration.Dashboard);
builder.Services.AddSingleton(appConfiguration.Storage);

if (StorageConnectionResolver.IsConfigured(appConfiguration.Storage))
{
    builder.Services.AddSingleton<PostgresConnectionFactory>();
    builder.Services.AddSingleton<IStorageSchemaInitializer, PostgresSchemaInitializer>();
    builder.Services.AddSingleton<IAppRepository, PostgresAppRepository>();
}
else
{
    builder.Services.AddSingleton<IStorageSchemaInitializer, NoOpStorageSchemaInitializer>();
    builder.Services.AddSingleton<IAppRepository, NoOpAppRepository>();
}

builder.Services.AddSingleton<IPolymarketApiErrorSink, RepositoryPolymarketApiErrorSink>();
builder.Services.AddHttpClient<IPolymarketDataApiClient, PolymarketDataApiClient>();
builder.Services.AddHttpClient<IPolymarketClobPublicClient, PolymarketClobPublicClient>();
builder.Services.AddHttpClient<IPolymarketGeoClient, PolymarketGeoClient>();
builder.Services.AddSingleton<ILeaderTradeCandidateQueue, InMemoryLeaderTradeCandidateQueue>();
builder.Services.AddSingleton<IWatchlistScanner, WatchlistScanner>();
builder.Services.AddSingleton<IRiskEngine, DefaultRiskEngine>();
builder.Services.AddSingleton<ISignalEngine, DefaultSignalEngine>();
builder.Services.AddSingleton<IPaperTradingEngine, DefaultPaperTradingEngine>();
builder.Services.AddSingleton<ISignalProcessor, SignalProcessor>();
builder.Services.AddSingleton<IPaperTradingProcessor, PaperTradingProcessor>();
builder.Services.AddHostedService<BotWorker>();

try
{
    Log.Information("Starting PolyCopyTrader service host.");
    Log.Information("Configuration summary:{NewLine}{ConfigSummary}", Environment.NewLine, AppOptionsValidator.ToSanitizedSummary(appConfiguration));

    if (!StorageConnectionResolver.IsConfigured(appConfiguration.Storage))
    {
        Log.Warning(
            "PostgreSQL connection string is not configured. Storage is disabled until {EnvVar} is set.",
            appConfiguration.Storage.ConnectionStringEnvironmentVariable);
    }

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
