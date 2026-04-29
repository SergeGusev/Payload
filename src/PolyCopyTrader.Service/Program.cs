using PolyCopyTrader.Service;
using Serilog;
using Serilog.Events;

var builder = Host.CreateApplicationBuilder(args);

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
builder.Services.AddHostedService<BotWorker>();

try
{
    Log.Information("Starting PolyCopyTrader service host.");
    var host = builder.Build();
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
