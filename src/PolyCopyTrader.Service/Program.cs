using PolyCopyTrader.Service;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.Configuration;
using PolyCopyTrader.Storage;
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
builder.Services.AddSingleton(appConfiguration.Polymarket);
builder.Services.AddSingleton(appConfiguration.Watchlist);
builder.Services.AddSingleton(appConfiguration.PaperTrading);
builder.Services.AddSingleton(appConfiguration.Dashboard);
builder.Services.AddSingleton(appConfiguration.Storage);
builder.Services.AddSingleton<SqliteConnectionFactory>();
builder.Services.AddSingleton<ISqliteSchemaInitializer, SqliteSchemaInitializer>();
builder.Services.AddSingleton<IAppRepository, SqliteAppRepository>();
builder.Services.AddHostedService<BotWorker>();

try
{
    Log.Information("Starting PolyCopyTrader service host.");
    Log.Information("Configuration summary:{NewLine}{ConfigSummary}", Environment.NewLine, AppOptionsValidator.ToSanitizedSummary(appConfiguration));

    var host = builder.Build();
    await host.Services.GetRequiredService<ISqliteSchemaInitializer>().InitializeAsync();
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
