using Microsoft.Extensions.Configuration;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Dashboard.Services;

public static class DashboardRepositoryFactory
{
    public static DashboardRuntime Create()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .Build();

        var appConfiguration = new AppConfiguration
        {
            Bot = configuration.GetSection("Bot").Get<BotOptions>() ?? new BotOptions(),
            Dashboard = configuration.GetSection("Dashboard").Get<DashboardOptions>() ?? new DashboardOptions(),
            Storage = configuration.GetSection("Storage").Get<StorageOptions>() ?? new StorageOptions(),
            Risk = configuration.GetSection("Risk").Get<RiskOptions>() ?? new RiskOptions(),
            PaperTrading = configuration.GetSection("PaperTrading").Get<PaperTradingOptions>() ?? new PaperTradingOptions(),
            Watchlist = configuration.GetSection("Watchlist").Get<WatchlistOptions>() ?? new WatchlistOptions(),
            Analytics = configuration.GetSection("Analytics").Get<AnalyticsOptions>() ?? new AnalyticsOptions(),
            Ipc = configuration.GetSection("Ipc").Get<IpcOptions>() ?? new IpcOptions()
        };

        IAppRepository repository;
        if (StorageConnectionResolver.IsConfigured(appConfiguration.Storage))
        {
            repository = new PostgresAppRepository(new PostgresConnectionFactory(appConfiguration.Storage));
        }
        else
        {
            repository = new NoOpAppRepository();
        }

        return new DashboardRuntime(
            repository,
            appConfiguration,
            StorageConnectionResolver.IsConfigured(appConfiguration.Storage));
    }
}

public sealed record DashboardRuntime(
    IAppRepository Repository,
    AppConfiguration Configuration,
    bool StorageConfigured);
