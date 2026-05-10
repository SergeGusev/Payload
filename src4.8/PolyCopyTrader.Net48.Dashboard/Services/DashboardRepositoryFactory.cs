using Microsoft.Extensions.Configuration;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket.Auth;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Dashboard.Services;

public static class DashboardRepositoryFactory
{
    public static DashboardRuntime Create()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();

        var appConfiguration = new AppConfiguration
        {
            Bot = configuration.GetSection("Bot").Get<BotOptions>() ?? new BotOptions(),
            PolymarketAuth = configuration.GetSection("PolymarketAuth").Get<PolymarketAuthOptions>() ?? new PolymarketAuthOptions(),
            Dashboard = configuration.GetSection("Dashboard").Get<DashboardOptions>() ?? new DashboardOptions(),
            Storage = configuration.GetSection("Storage").Get<StorageOptions>() ?? new StorageOptions(),
            Risk = configuration.GetSection("Risk").Get<RiskOptions>() ?? new RiskOptions(),
            PaperTrading = configuration.GetSection("PaperTrading").Get<PaperTradingOptions>() ?? new PaperTradingOptions(),
            LiveTrading = configuration.GetSection("LiveTrading").Get<LiveTradingOptions>() ?? new LiveTradingOptions(),
            Watchlist = configuration.GetSection("Watchlist").Get<WatchlistOptions>() ?? new WatchlistOptions(),
            Analytics = configuration.GetSection("Analytics").Get<AnalyticsOptions>() ?? new AnalyticsOptions(),
            TraderDiscovery = configuration.GetSection("TraderDiscovery").Get<TraderDiscoveryOptions>() ?? new TraderDiscoveryOptions(),
            OnChainIngestion = configuration.GetSection("OnChainIngestion").Get<OnChainIngestionOptions>() ?? new OnChainIngestionOptions(),
            Ipc = configuration.GetSection("Ipc").Get<IpcOptions>() ?? new IpcOptions()
        };

        if (!StorageConnectionResolver.IsConfigured(appConfiguration.Storage))
        {
            throw new InvalidOperationException(
                "Dashboard storage is not configured. Set " +
                appConfiguration.Storage.ConnectionStringEnvironmentVariable +
                " or Storage:ConnectionString.");
        }

        var connectionFactory = new PostgresConnectionFactory(appConfiguration.Storage);
        var repository = new PostgresAppRepository(connectionFactory);
        var authService = new DisabledPolymarketAuthService();

        return new DashboardRuntime(
            repository,
            appConfiguration,
            StorageConnectionResolver.IsConfigured(appConfiguration.Storage),
            authService);
    }
}

public sealed record DashboardRuntime(
    IAppRepository Repository,
    AppConfiguration Configuration,
    bool StorageConfigured,
    IPolymarketAuthService AuthService);
